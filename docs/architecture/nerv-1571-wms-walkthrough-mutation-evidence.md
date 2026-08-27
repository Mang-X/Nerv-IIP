# NERV-1571 WMS 走查事实 mutation 证据

本文是一次冻结的 `Report`，只记录 NERV-1571 WMS 走查事实 proof 的变异鉴别力，不是
FullStack/FullChain 运行报告，也不把自建事实升级为 `PublicContract`。

## 基线与方法

- 被测代码基线：`headSha=testedSha=22889ea1902dc20b457c563543de5621ab623b49`（本地
  checkout）；该提交包含本票代码和 mock
  的 scope option/readback 保护。每项 mutation 前后均用 `git status --short` 确认恢复为干净树。
- 绿基线命令：
  `CI=1 PLAYWRIGHT_BUSINESS_CONSOLE_PORT=5160 pnpm -C frontend/apps/business-console exec playwright test e2e/issue1912-wms-walkthrough-facts.spec.ts --project=desktop --workers=1 --reporter=line`
  结果：`6 passed`。
- mutation 采用一次性 `apply_patch` 改动，运行下表命令后立即反向 patch；表中 `exit=1`
  是预期的 RED，报告中的错误摘录来自该次运行的标准输出。没有将 mutant 留在分支。

## Mutation 矩阵

| 变异（临时 diff） | 命令 | 可审 RED 输出 |
| --- | --- | --- |
| 删除 `facts.ts` 入库分支的 `await selectWmsPageOption(options.page, options.selection.site)` | `CI=1 PLAYWRIGHT_BUSINESS_CONSOLE_PORT=5161 pnpm -C frontend/apps/business-console exec playwright test e2e/issue1912-wms-walkthrough-facts.spec.ts -g '入库必须显式选择范围和已加载工厂' --project=desktop --workers=1 --reporter=line` | `exit=1`；`WMS list /api/business-console/v1/wms/inbound-orders query facts did not match`，实际请求 `siteCode=`，不是 authority 的 `SITE-001` |
| 删除 `facts.ts` 出库分支的 `await selectWmsScopeOption(options.page, options.selection.scope)` | `CI=1 PLAYWRIGHT_BUSINESS_CONSOLE_PORT=5162 pnpm -C frontend/apps/business-console exec playwright test e2e/issue1912-wms-walkthrough-facts.spec.ts -g '显式作业范围选择后' --project=desktop --workers=1 --reporter=line` | `exit=1`；`WMS list /api/business-console/v1/wms/outbound-orders query facts did not match`，实际请求 `scopeId=` |
| 删除 `refreshWmsListAndConfirm` 的 `assertWmsListQueryFacts(...)` | `CI=1 PLAYWRIGHT_BUSINESS_CONSOLE_PORT=5163 pnpm -C frontend/apps/business-console exec playwright test e2e/issue1912-wms-walkthrough-facts.spec.ts -g '显式作业范围选择后' --project=desktop --workers=1 --reporter=line` | `exit=1`；`expect(received).rejects.toThrow()`，`Received promise resolved instead of rejected` |
| 删除 `selectWmsPageOption` 选中后的触发器 readback（`toContainText`） | `CI=1 PLAYWRIGHT_BUSINESS_CONSOLE_PORT=5164 pnpm -C frontend/apps/business-console exec playwright test e2e/issue1912-wms-walkthrough-facts.spec.ts -g '工厂选择按公开编码匹配' --project=desktop --workers=1 --reporter=line` | `exit=1`；`Received promise resolved instead of rejected`，缺失 readback 的 fixture 没有再被接受 |
| 将 `waitForUniqueVisibleOption` 的稳定采样从 `stableSamples >= 2` 改为 `>= 1` | `CI=1 PLAYWRIGHT_BUSINESS_CONSOLE_PORT=5165 pnpm -C frontend/apps/business-console exec playwright test e2e/issue1912-wms-walkthrough-facts.spec.ts -g '工厂目录首项后续变为重复编码' --project=desktop --workers=1 --reporter=line` | `exit=1`；预期 `expected one catalog option, found 2`，实际已提前点击后在 `toHaveAttribute` 处失败，证明稳定性断言被鉴别 |
| 删除 `withWmsInitialListResponseGuard` 对首个 response 的 `assertWmsInitialListResponse(...)` | `CI=1 PLAYWRIGHT_BUSINESS_CONSOLE_PORT=5166 pnpm -C frontend/apps/business-console exec playwright test e2e/issue1912-wms-walkthrough-facts.spec.ts -g '首个 WMS 列表响应' --project=desktop --workers=1 --reporter=line` | `exit=1`；`Received promise resolved instead of rejected`，首个 503 不再能被后续 200 掩盖 |
| 删除 `assertExpectedQueryFacts` 的 kind/path/forbidden-key guard（联合合同变异） | `CI=1 pnpm -C frontend/apps/business-console exec vitest run src/issue1912WmsWalkthroughFacts.contract.test.ts --reporter=dot` | `exit=1`；`1 failed, 5 passed`，联合合同测试预期 `unexpected list path|invalid forbidden query key contract`，但变异后收到 `keyword did not match expected DO-WALK-001` |

这些结果覆盖入库 site、出库 scope、刷新 wrapper、页面 readback、异步目录第二采样、首个
列表 response 的 status/path，以及 kind/path/query/forbidden-key 互斥组合。合同矩阵另对同一
keyword 的 `organizationId`、`environmentId`、`scopeKind`、`scopeId`、`skip`、`take`、
`siteCode`、缺失/重复 `keyword`、错误 path/status 做了独立 RED 断言。

报告提交后的最终代码校验在 `headSha=testedSha=5cdfdfce86eed46f92bad36f776c6db78ef714c4`
（本地 checkout）完成：WMS 合同 `6/6`、WMS mock Playwright `6/6`、相关 action/policy 与
WMS mock Playwright `30/30`、业务控制台 Vitest `191 files / 2207 tests`、typecheck、目标
format 和 lint 均成功；real-machine 文件为 `1 passed / 1 skipped`，skip 是缺少 managed
FullStack 环境。

本报告的 mutation 运行是本地 Playwright mock fixture/Vitest 合同测试；它不提供真实身份、
WMS provider、HTTP 拓扑、数据库或 cleanup 证据。真实 managed FullStack/FullChain 和 GitHub
#1912 的无空态 ERP purchase+sales gate 仍须由后置 operator/workflow 运行。
