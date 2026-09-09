# #2949 真机走查取证

- PR：#3115
- 被测 head：`f9dadad73277fe50cf90b7f11a82e5015f865d9c`
- 视口：1440×900（与 #2706 抓到「…如 DEV」截断时同一条件）
- 浏览器：Playwright + 真实 Chromium（非 headless shell 的 jsdom 替身）
- 取证脚本：`frontend/apps/business-console/e2e/issue2949-placeholder-hint.spec.ts`（随 PR 合入，可重跑）
- 跑法：`NERV_IIP_OUT_DIR=<dir> pnpm exec playwright test e2e/issue2949 --project=desktop`

`s1-` 前缀 = 第 1 轮；重拍不复用文件名。
