# #2949 真机走查取证

- PR：#3115
- 视口：1440×900（与 #2706 抓到「…如 DEV」截断时同一条件）
- 浏览器：Playwright 驱动的真实 Chromium
- 取证脚本：`frontend/apps/business-console/e2e/issue2949-placeholder-hint.spec.ts`（随 PR 合入，可重跑）
- 跑法：`NERV_IIP_OUT_DIR=<dir> pnpm exec playwright test e2e/issue2949 --project=desktop`

## 轮次

轮次前缀不复用文件名，旧轮产物保留可对照。

- `s1-*`：第 1 轮，被测 head `f9dadad73`。量渲染宽 3 处（其中 `#op-control` 恒真）。
- `s2-*`：第 2 轮，被测 head `ff6dc42a471d08347cfb9089254e8983bc27a444`。量渲染宽扩到 5 处，恒真断言已移除；`measureHint` 改精确定位 + `toHaveCount(1)`。

## 两处预填字段（判据假阳，非截断缺陷）

`s2-03` 控制键预填 `INHOUSE`、`s2-05` 内容类型预填 `application/pdf`——它们的 placeholder
对用户从来不可见（placeholder 只在空值时渲染），因此不在「5 处真实截断缺陷」之内。
