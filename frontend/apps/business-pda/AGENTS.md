# AGENTS.md — business-pda（PDA 移动端）

> 根 `AGENTS.md` 仍然适用；本文件只补充 PDA 子树的差异。
> business-pda = 车间手持 PDA（Android WebView / Capacitor 8），扫码优先的
> WMS / MES / 质量 / 设备作业端。交互范式与页面矩阵：
> `docs/architecture/mobile-pda-module-product-design.md`。

## Commands

```powershell
pnpm -C frontend --filter @nerv-iip/business-pda typecheck
pnpm -C frontend --filter @nerv-iip/business-pda test
pnpm -C frontend --filter @nerv-iip/business-pda build      # 含 vue-tsc
pnpm -C frontend --filter @nerv-iip/business-pda dev        # 端口 5126；PDA 不在 Aspire AppHost 里，需单独起
pnpm -C frontend --filter @nerv-iip/business-pda e2e        # Playwright mock e2e：自动起 webServer(端口 5176)，page.route 全 mock，无需后端
pnpm -C frontend --filter @nerv-iip/business-pda e2e:live   # 真栈 e2e(playwright.live.config.ts)，需要整栈在跑
pnpm -C frontend --filter @nerv-iip/business-pda cap:sync   # build + cap sync android；android/ 工程本地生成，不入库
```

## 测试层次 — 哪一层算"验证过"

四层，逐层升级，**不可越级声称**：

1. vitest 组件/单元测试（`vp test`）
2. Playwright **mock e2e**（`e2e/`，真实 Chromium 移动视口，网关全 mock）
3. **真栈 e2e**（`e2e-live/`，真后端整栈）
4. **真机** = 目标 PDA + APK + 实体扫码枪 —— 浏览器/模拟器里跑的只能叫
   e2e，不能声称"真机验证"。

分层定义、spec 清单、冒烟命令：`docs/architecture/mobile-pda-testing-and-smoke.md`。
Capacitor 架构与打包部署：`docs/architecture/mobile-pda-capacitor-architecture.md`、
`mobile-pda-deployment.md`。

## Hard Rules

1. **测试 setup 已全局 `enableAutoUnmount(afterEach)`**（`src/test/setup.ts`）。
   不要再手动 `unmount()`、不要加 afterEach 清 body —— 重复卸载会让
   teleport 组件在 removeFragment 阶段崩掉整个测试文件。
2. **写操作幂等键跨重试必须稳定。** `makeIdempotencyKey()` 每个操作意图只
   生成一次（存 ref 复用，见 `pages/mes/*` 的 `operationKey` 模式）；传输层
   超时重试（`src/api/request-timeout.ts`）不得重新生成幂等键。
3. **api-client 定制只走 `configureApiClient({ fetch })` 唯一注入点**
   （`src/api/`），一处覆盖全部 client；不要 patch `window.fetch`。
4. **组件只用 `@nerv-iip/ui-mobile`**（`NvMobile*` / `Nv*` 移动专名）。PC 端
   `@nerv-iip/ui` 组件不进 PDA 页面；跨表面需求 = 在 ui-mobile 建移动版。
5. **数字/测量值录入复用 NvNumberKeyboard 模式**（只读单元格触发、防系统
   键盘弹出）——参考 `components/quality/QualityExecuteStep.vue` 与
   design-system 文档站 `mobile/number-keyboard`。
