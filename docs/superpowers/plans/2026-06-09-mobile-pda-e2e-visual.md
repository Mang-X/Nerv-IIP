# PDA e2e + 视觉/布局回归测试 Implementation Plan（Plan 1.5）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** 给 `frontend/apps/business-pda` 补一套 Playwright e2e + 移动端视觉/布局回归，覆盖登录→首页真实流程，并通过一个**组件画廊页**让 `@nerv-iip/ui-mobile` 全部 5 个组件都被真实浏览器交互 + 布局/触控/安全区/暗色断言覆盖。

**Architecture:** 完全镜像 console/business-console 既有 Playwright 约定：`playwright.config.ts`（mobile 视口 + `vp dev` webServer + `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`），e2e 全程 `page.route` Mock 网关（无需后端），`seedStoredSession` 注入 localStorage 跳过登录。视觉断言走 `getComputedStyle`/`getBoundingClientRect`（仓库无像素快照基线，沿用计算样式 smoke）。新增 dev 画廊页渲染所有触摸组件供组件级 e2e。

**Tech Stack:** @playwright/test@^1.60.0 / vite-plus(`vp dev`) / Vue 3 / 既有 jsdom 单测不变。

---

## 范围与门禁口径

**交付：** business-pda 的 `playwright.config.ts` + `e2e/`（fixtures + 2 个 spec）+ dev 画廊页 + 真机冒烟清单文档。覆盖：登录流程、首页（扫码条/应用墙/我的任务空态）、5 个 ui-mobile 组件交互、移动视口无横向溢出、触控尺寸、安全区最小内边距、暗色渲染。

**不在范围：** 像素级视觉快照（仓库无基线，且 PDA 设计稿未定稿——留作后续）；真机键盘楔入扫码与 Capacitor APK 内 WebView（CI 测不到，进**手动冒烟清单**）；M2+ 业务页 e2e（随各页落地）。

**门禁口径（重要，沿用仓库 Playwright caveat）：** Playwright 需 Chromium。运行 `playwright test` 前需 `playwright install chromium` 或设 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`。若本机浏览器不可用，**如实报告环境阻塞、不伪造结果**（同 readiness Phase 8 Task 9 口径）。在浏览器不可用时，最低验证是 `playwright test --list`（解析/发现 spec，不启浏览器）+ 既有 `typecheck`/`build` 仍绿。

**端口：** business-pda dev=5126；e2e webServer 用独立端口 **5176**（`PLAYWRIGHT_BUSINESS_PDA_PORT` 默认 5176），避免与 business-console e2e(5126) 及 pda dev(5126) 撞口。

## 关键事实（零猜测，执行者照用）

- PDA auth localStorage key：`'nerv-iip.business-pda.auth'`（见 `stores/auth.ts`）。
- PDA 登录页选择器：输入有 `aria-label="账号"`/`aria-label="密码"`，提交按钮文案 `登录`。登录成功跳 `/`。
- 首页标题：`工作台`（`pages/index.vue` header `<h1>工作台</h1>`）；我的任务空态文案：`暂无分配给你的任务`；应用墙含 `收货入库`/`报工` 等且当前全 `disabled`（`PDA_TASK_KINDS` 全 `routeReady:false`）。
- auth envelope：api-client `assertData` 要求响应体为 `{ success: true, data: <payload> }`。登录走 `POST /api/console/v1/auth/login`，会话恢复走 `/api/console/v1/auth/refresh`，`/me` 走 `/api/console/v1/auth/me`。
- 路由 routesFolder 的 exclude 含 `**/components/**/*`——画廊页**不要**放在名为 `components` 的目录或文件里；用 `pages/design-system/gallery.vue`（路由 `/design-system/gallery`，meta 不设 `requiresAuth`，e2e 可直达）。
- 安全区：Playwright 不模拟真机 `env(safe-area-inset-*)`（非刘海设备 inset=0）。`mobile.css` 用 `max(0.75rem, env(...))`/`max(0.5rem, env(...))`，所以**可断言的是 fallback 最小值生效**（header padding-top ≥ 12px、底栏 padding-bottom ≥ 8px），真机真实 inset 渲染进手动冒烟清单。

## 文件结构地图

```
frontend/apps/business-pda/
  package.json                       # 改：+ "e2e" script、+ @playwright/test devDep
  playwright.config.ts               # 新：mobile 视口 + vp dev webServer(5176)
  e2e/
    fixtures.ts                      # 新：mock 网关 + seedStoredSession + 布局/样式断言 helper
    app-flow.spec.ts                 # 新：登录→首页 + 首页交互
    ui-mobile.spec.ts                # 新：画廊页 5 组件交互 + 视觉/布局/暗色
  src/pages/design-system/
    gallery.vue                      # 新：dev 画廊，渲染全部 ui-mobile 组件
docs/architecture/
  mobile-pda-testing-and-smoke.md    # 新：e2e 运行说明 + 真机手动冒烟清单
docs/superpowers/specs/2026-06-09-mobile-pda-design.md  # 改：§13 增 e2e/视觉口径
```

---

## Task 1: Playwright 脚手架（config + deps + e2e script + fixtures）

**Files:**
- Modify: `frontend/apps/business-pda/package.json`
- Create: `frontend/apps/business-pda/playwright.config.ts`
- Create: `frontend/apps/business-pda/e2e/fixtures.ts`

- [ ] **Step 1: 装 Playwright + 加 e2e script**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda add -D @playwright/test@^1.60.0`
然后在 `frontend/apps/business-pda/package.json` 的 `scripts` 加：`"e2e": "playwright test"`（与 console/business-console 一致）。

- [ ] **Step 2: 写 playwright.config.ts**

`frontend/apps/business-pda/playwright.config.ts`：

```typescript
import { defineConfig, devices } from '@playwright/test'

const executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
const port = Number(process.env.PLAYWRIGHT_BUSINESS_PDA_PORT ?? 5176)
const baseURL = `http://127.0.0.1:${port}`

export default defineConfig({
  testDir: './e2e',
  forbidOnly: !!process.env.CI,
  fullyParallel: true,
  reporter: 'list',
  use: {
    baseURL,
    launchOptions: executablePath ? { executablePath } : undefined,
    trace: 'on-first-retry',
  },
  webServer: {
    command: `vp dev --host 127.0.0.1 --port ${port}`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [
    {
      name: 'mobile',
      use: { ...devices['Pixel 5'], viewport: { width: 390, height: 844 } },
    },
  ],
})
```

- [ ] **Step 3: 写 e2e/fixtures.ts（mock + helper）**

`frontend/apps/business-pda/e2e/fixtures.ts`：

```typescript
import { expect, type Page, type Route } from '@playwright/test'

export const STORAGE_KEY = 'nerv-iip.business-pda.auth'

export const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'operator01',
  email: 'operator01@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
}

export const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

export function envelope<T>(data: T) {
  return { success: true, message: null, data }
}

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/** Mock the console auth endpoints the PDA app depends on (login + refresh + me). */
export async function routeConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  if (pathname === '/api/console/v1/auth/login' || pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }
  if (pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }
  if (pathname === '/api/console/v1/auth/logout') {
    return fulfillJson(route, envelope({}))
  }
  return fulfillJson(route, envelope({}))
}

/** Mock any business-console gateway call the home may make (none required for the foundation home). */
export async function routeBusinessConsoleApi(route: Route) {
  return fulfillJson(route, envelope({}))
}

/** Seed a stored session so guarded routes load without going through the login form. */
export async function seedStoredSession(page: Page, colorMode?: 'light' | 'dark') {
  await page.addInitScript(
    ({ key, stored, mode }) => {
      localStorage.setItem(key, JSON.stringify(stored))
      if (mode) localStorage.setItem('nerv-iip-color-mode', mode)
    },
    {
      key: STORAGE_KEY,
      stored: { principal, refreshToken: session.refreshToken, sessionId: session.sessionId },
      mode: colorMode,
    },
  )
}

export async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow).toBeLessThanOrEqual(1)
}

/** Every enabled interactive control must meet the 44px touch-target floor. */
export async function expectTouchTargets(page: Page) {
  const tooSmall = await page.evaluate(() => {
    const els = [...document.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input')]
    return els
      .map((el) => {
        const r = el.getBoundingClientRect()
        return { tag: el.tagName, w: r.width, h: r.height }
      })
      .filter((m) => m.w > 0 && m.h > 0 && (m.w < 44 || m.h < 44))
  })
  expect(tooSmall).toEqual([])
}
```

- [ ] **Step 4: 验证脚手架解析**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec playwright test --list`
Expected: 列出 0 个 spec（spec 还没写），命令本身不报解析错误。若 Playwright 报缺浏览器，这一步 `--list` 不需要浏览器，应仍可列出。
Run: `pnpm -C frontend --filter @nerv-iip/business-pda typecheck` → PASS（新增 config 不破坏 typecheck）。

- [ ] **Step 5: Commit**

```bash
git add frontend/apps/business-pda/package.json frontend/apps/business-pda/playwright.config.ts frontend/apps/business-pda/e2e/fixtures.ts frontend/pnpm-lock.yaml
git commit -m "test(business-pda): scaffold Playwright e2e (config + gateway mock fixtures)"
```

---

## Task 2: 登录→首页 e2e（app-flow.spec.ts）

**Files:**
- Create: `frontend/apps/business-pda/e2e/app-flow.spec.ts`

- [ ] **Step 1: 写 spec**

`frontend/apps/business-pda/e2e/app-flow.spec.ts`：

```typescript
import { expect, test } from '@playwright/test'
import {
  expectNoHorizontalOverflow,
  expectTouchTargets,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('logs in and lands on the workbench home', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('账号').fill('operator01')
  await page.getByLabel('密码').fill('Operator123!')
  await page.getByRole('button', { name: '登录' }).click()

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()
})

test('home shows scan bar, my-tasks empty state and a disabled app wall', async ({ page }) => {
  await seedStoredSession(page)
  await page.goto('/')

  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()
  // scan bar focus input present
  await expect(page.locator('input[placeholder^="扫描"]')).toBeVisible()
  // my-tasks empty state (no fake data)
  await expect(page.getByText('暂无分配给你的任务')).toBeVisible()
  // app wall labels render and are disabled until M2 pages land
  await expect(page.getByRole('button', { name: '收货入库' })).toBeDisabled()
  await expect(page.getByRole('button', { name: '报工' })).toBeDisabled()

  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('clicking a not-ready app-wall entry does not navigate away', async ({ page }) => {
  await seedStoredSession(page)
  await page.goto('/')
  await page.getByRole('button', { name: '收货入库' }).click({ force: true })
  await expect(page).toHaveURL('/')
})
```

- [ ] **Step 2: 跑 spec（浏览器可用时）**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda e2e -- app-flow.spec.ts`
Expected: 3 passed。
若报缺 Chromium：先 `pnpm -C frontend --filter @nerv-iip/business-pda exec playwright install chromium`，或设 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 后重试。仍不可用则记录环境阻塞，并至少 `--list` 确认本 spec 被发现。

- [ ] **Step 3: Commit**

```bash
git add frontend/apps/business-pda/e2e/app-flow.spec.ts
git commit -m "test(business-pda): e2e for login flow + home scan/app-wall/empty-state"
```

---

## Task 3: 组件画廊页 + ui-mobile 组件级 e2e（视觉/布局/暗色）

**Files:**
- Create: `frontend/apps/business-pda/src/pages/design-system/gallery.vue`
- Create: `frontend/apps/business-pda/e2e/ui-mobile.spec.ts`

- [ ] **Step 1: 写画廊页（渲染全部 5 个组件）**

`frontend/apps/business-pda/src/pages/design-system/gallery.vue`（路由 `/design-system/gallery`，无 `requiresAuth`，dev 可视化 + e2e 载体；用 `data-testid` 锚点便于断言）：

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { definePage } from 'vue-router/auto'
import { AppShellMobile, ScanBar, ListRow, BottomSheet, Result } from '@nerv-iip/ui-mobile'

definePage({ meta: { title: 'UI Mobile 组件库' } })

const lastScan = ref('')
const sheetOpen = ref(false)
const listClicked = ref(false)
</script>

<template>
  <AppShellMobile>
    <template #header>
      <div class="px-4 py-3"><h1 class="text-lg font-semibold">UI Mobile 组件库</h1></div>
    </template>

    <div class="space-y-6 p-4">
      <section data-testid="scan-section">
        <ScanBar placeholder="扫描条码" @scan="(v: string) => (lastScan = v)" />
        <p data-testid="scan-result" class="mt-1 text-sm text-muted-foreground">{{ lastScan }}</p>
      </section>

      <section data-testid="list-section">
        <ListRow title="收货单 RO-2026-001" subtitle="待收货 · 3 行" @select="listClicked = true" />
        <ListRow title="只读行" :interactive="false" />
        <p data-testid="list-clicked">{{ listClicked ? 'clicked' : 'idle' }}</p>
      </section>

      <section data-testid="sheet-section">
        <button data-testid="open-sheet" class="min-h-touch rounded-lg bg-primary px-4 text-primary-foreground" @click="sheetOpen = true">打开抽屉</button>
        <BottomSheet :open="sheetOpen" title="选择库位" @update:open="(v: boolean) => (sheetOpen = v)">
          <p>抽屉内容</p>
        </BottomSheet>
      </section>

      <section data-testid="result-success"><Result status="success" title="过账成功" description="收货单已完成" /></section>
      <section data-testid="result-error"><Result status="error" title="过账失败" /></section>
    </div>

    <template #footer>
      <div class="px-4 py-2 text-center text-sm text-muted-foreground">底部导航占位</div>
    </template>
  </AppShellMobile>
</template>
```

- [ ] **Step 2: 写组件级 e2e + 视觉/布局/暗色 spec**

`frontend/apps/business-pda/e2e/ui-mobile.spec.ts`：

```typescript
import { expect, test } from '@playwright/test'
import { expectNoHorizontalOverflow, expectTouchTargets, seedStoredSession } from './fixtures'

const GALLERY = '/design-system/gallery'

test('all ui-mobile components render and the mobile layout has no overflow', async ({ page }) => {
  await page.goto(GALLERY)
  await expect(page.getByRole('heading', { name: 'UI Mobile 组件库' })).toBeVisible()
  await expect(page.getByTestId('scan-section')).toBeVisible()
  await expect(page.getByText('过账成功')).toBeVisible()
  await expect(page.getByText('过账失败')).toBeVisible()
  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('ScanBar emits the scanned value (keyboard-wedge: type + Enter)', async ({ page }) => {
  await page.goto(GALLERY)
  const input = page.locator('input[placeholder="扫描条码"]')
  await input.click()
  await input.type('SKU-12345')
  await input.press('Enter')
  await expect(page.getByTestId('scan-result')).toHaveText('SKU-12345')
})

test('ListRow select fires only for the interactive row', async ({ page }) => {
  await page.goto(GALLERY)
  await expect(page.getByTestId('list-clicked')).toHaveText('idle')
  await page.getByText('收货单 RO-2026-001').click()
  await expect(page.getByTestId('list-clicked')).toHaveText('clicked')
})

test('BottomSheet opens and closes (Escape dismiss)', async ({ page }) => {
  await page.goto(GALLERY)
  await expect(page.getByText('抽屉内容')).toHaveCount(0)
  await page.getByTestId('open-sheet').click()
  await expect(page.getByText('抽屉内容')).toBeVisible()
  await expect(page.getByText('选择库位')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByText('抽屉内容')).toHaveCount(0)
})

test('AppShellMobile applies the safe-area minimum padding on header and footer', async ({ page }) => {
  await page.goto(GALLERY)
  const pads = await page.evaluate(() => {
    const header = document.querySelector('[data-shell="header"]') as HTMLElement
    const footer = document.querySelector('[data-shell="footer"]') as HTMLElement
    const px = (v: string) => Number.parseFloat(v)
    return {
      headerTop: px(getComputedStyle(header).paddingTop),
      footerBottom: px(getComputedStyle(footer).paddingBottom),
    }
  })
  // max(0.75rem, env(...)) / max(0.5rem, env(...)) — env=0 on emulated devices → fallback minimums
  expect(pads.headerTop).toBeGreaterThanOrEqual(12)
  expect(pads.footerBottom).toBeGreaterThanOrEqual(8)
})

test('dark mode renders a dark surface (token wiring)', async ({ page }) => {
  await seedStoredSession(page, 'dark')
  await page.goto(GALLERY)
  const result = await page.evaluate(() => {
    const isDark = document.documentElement.classList.contains('dark')
    const bg = getComputedStyle(document.body).backgroundColor
    // parse rgb(...) -> perceived lightness
    const m = bg.match(/\d+(\.\d+)?/g)?.map(Number) ?? [255, 255, 255]
    const lightness = (0.299 * m[0] + 0.587 * m[1] + 0.114 * m[2]) / 255
    return { isDark, lightness }
  })
  expect(result.isDark).toBe(true)
  expect(result.lightness).toBeLessThan(0.5)
})
```

- [ ] **Step 3: 跑 spec（浏览器可用时）**

Run: `pnpm -C frontend --filter @nerv-iip/business-pda e2e -- ui-mobile.spec.ts`
Expected: 6 passed。浏览器不可用时同 Task 2 的降级口径（`--list` + 记录环境阻塞）。
Run: `pnpm -C frontend --filter @nerv-iip/business-pda typecheck` 与 `... build` → PASS（画廊页纳入构建，确认无 unused import / 类型错误）。

- [ ] **Step 4: Commit**

```bash
git add frontend/apps/business-pda/src/pages/design-system/gallery.vue frontend/apps/business-pda/e2e/ui-mobile.spec.ts
git commit -m "test(business-pda): ui-mobile component gallery + e2e (interaction/layout/dark)"
```

---

## Task 4: 文档（e2e 运行说明 + 真机手动冒烟清单）

**Files:**
- Create: `docs/architecture/mobile-pda-testing-and-smoke.md`
- Modify: `docs/superpowers/specs/2026-06-09-mobile-pda-design.md`（§13）

- [ ] **Step 1: 写测试与冒烟文档**

`docs/architecture/mobile-pda-testing-and-smoke.md`，至少含：
- **测试层次**：jsdom 单元/组件测试（`vp test`，行为/事件/标记）；Playwright e2e（真实浏览器，移动视口，流程 + 布局/触控/安全区/暗色，网关全 Mock）。
- **运行命令**：
  ```
  pnpm -C frontend --filter @nerv-iip/business-pda exec playwright install chromium
  pnpm -C frontend --filter @nerv-iip/business-pda e2e
  pnpm -C frontend --filter @nerv-iip/business-pda e2e -- app-flow.spec.ts
  ```
  浏览器缺失时设 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`；不可用则如实报环境阻塞（同 readiness 口径），最低用 `playwright test --list`。
- **e2e 能/不能覆盖什么**：能——流程、DOM 交互、计算样式布局、无溢出、触控 ≥44px、安全区 fallback 最小值、暗色 token；不能——真机真实 `env(safe-area-inset-*)`、硬件扫码枪键盘楔入、Capacitor APK 内 WebView、真机手势/滚动惯性。
- **真机手动冒烟清单（每次发版前在目标 PDA 上勾）**：
  1. 安装 APK 启动，登录成功，首页三段（顶栏/内容/底栏）无遮挡、刘海/手势条不压内容。
  2. 硬件扫码枪扫一段条码 → 扫码条捕获并显示，焦点常驻、失焦自动回抢。
  3. 应用墙触控目标够大、单手拇指可达；disabled 项不可点。
  4. 暗色/动态主色切换后整屏一致；横竖屏（若启用）无错位。
  5. 弱网/断网下写操作有清晰失败反馈（M2 起逐页验证）。

- [ ] **Step 2: 更新 spec §13**

在 `docs/superpowers/specs/2026-06-09-mobile-pda-design.md` §13 增一段：门禁除 typecheck/test/build 外，新增 Playwright e2e（移动视口流程 + 视觉/布局 smoke，网关 Mock），运行与降级口径见 `docs/architecture/mobile-pda-testing-and-smoke.md`；像素级视觉快照与真机扫码进手动冒烟清单。

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/mobile-pda-testing-and-smoke.md docs/superpowers/specs/2026-06-09-mobile-pda-design.md
git commit -m "docs(pda): e2e/visual testing layers + real-device smoke checklist"
```

---

## Task 5: 验收 + 推送

- [ ] **Step 1: 门禁**

Run（全绿）：
```
pnpm -C frontend --filter @nerv-iip/business-pda typecheck
pnpm -C frontend --filter @nerv-iip/business-pda test
pnpm -C frontend --filter @nerv-iip/business-pda build
pnpm -C frontend --filter @nerv-iip/business-pda exec playwright test --list
```
浏览器可用时另跑全量 `... e2e`（预期 9 passed：app-flow 3 + ui-mobile 6）；不可用则记录环境阻塞、附 `--list` 输出。

- [ ] **Step 2: 推送到 PR #365**

```bash
git push origin claude/happy-chaum-c9eb8a
```

---

## Self-Review

- **覆盖**：登录流程(Task2) + 首页交互/布局(Task2) + 5 组件交互/视觉/暗色(Task3,画廊页) + 文档/冒烟(Task4)。组件级 e2e 通过画廊页覆盖 ScanBar/ListRow/BottomSheet/Result/AppShellMobile，回答"组件是否有 e2e/视觉测试"。
- **占位符**：无 TODO；每步含完整代码/命令。环境阻塞口径明确（非占位）。
- **一致性**：STORAGE_KEY、登录选择器(`账号`/`密码`/`登录`)、首页文案(`工作台`/`暂无分配给你的任务`)、画廊路由(`/design-system/gallery`，避开 `components` exclude)、安全区 fallback 阈值(12/8px) 与已落地代码一致；端口 5176 避撞。
- **诚实边界**：像素快照与真机扫码/inset 明确不在 e2e 内，进手动冒烟清单。
```

