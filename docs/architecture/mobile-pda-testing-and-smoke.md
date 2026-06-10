# PDA（business-pda）测试层次与真机冒烟清单

> 事实依据：`frontend/apps/business-pda/`（`playwright.config.ts`、`e2e/`、`src/pages/design-system/gallery.vue`）、
> `docs/superpowers/specs/2026-06-09-mobile-pda-design.md` §13、仓库既有 Playwright caveat（同
> `docs/architecture/implementation-readiness.md` Phase 8 浏览器不可用降级口径）。

## 1. 测试层次

PDA 测试分两层，职责互补、不重叠：

1. **jsdom 单元/组件测试**（`vp test run src`）
   - 跑在 jsdom，无真实浏览器。覆盖：组件标记/行为/事件、store 逻辑、登录/首页页面的渲染与守卫断言。
   - 快、确定性高；但**测不到真实布局/计算样式、触控尺寸、安全区、暗色渲染、跨页导航**。

2. **Playwright e2e**（`playwright test`，真实 Chromium，移动视口 390×844 / Pixel 5）
   - 全程 `page.route` Mock BusinessGateway/console 网关（见 `e2e/fixtures.ts`），**无需后端**。
   - `seedStoredSession` 注入 `localStorage`（auth key `nerv-iip.business-pda.auth` + 可选
     `nerv-iip-color-mode`）跳过登录表单，直达受保护路由。
   - 覆盖：登录→首页真实流程、首页扫码条/应用墙/我的任务空态、`@nerv-iip/ui-mobile` 全部 5 个组件
     （AppShellMobile / ScanBar / ListRow / BottomSheet / Result，经 `/design-system/gallery` 画廊页载体）
     的真实交互，以及视觉/布局 smoke。

### e2e spec 清单（9 个用例）

- `e2e/app-flow.spec.ts`（3）：登录落地工作台；首页扫码条/空态/禁用应用墙 + 无溢出 + 触控尺寸；
  点击未就绪应用墙项不跳转。
- `e2e/ui-mobile.spec.ts`（6）：5 组件渲染 + 无溢出 + 触控尺寸；ScanBar 键盘楔入（type+Enter）发值；
  ListRow 仅交互行触发 select；BottomSheet 打开 + Escape 关闭；AppShellMobile 安全区 fallback 最小内边距；
  暗色 token 接线（`.dark` + body 深色背景）。

## 2. 运行命令

```bash
# 首次需安装浏览器（或设 PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH 指向已有 Chromium）
pnpm -C frontend --filter @nerv-iip/business-pda exec playwright install chromium

# 全量 e2e（mobile project，自动起 vp dev webServer，端口 5176）
pnpm -C frontend --filter @nerv-iip/business-pda e2e

# 单文件
pnpm -C frontend --filter @nerv-iip/business-pda e2e -- app-flow.spec.ts
pnpm -C frontend --filter @nerv-iip/business-pda e2e -- ui-mobile.spec.ts

# 不启浏览器，仅发现/解析 spec（浏览器不可用时的最低验证）
pnpm -C frontend --filter @nerv-iip/business-pda exec playwright test --list
```

- **端口**：e2e webServer 用独立端口 **5176**（`PLAYWRIGHT_BUSINESS_PDA_PORT` 默认 5176），
  避免与 PDA dev(5126) 及 business-console e2e(5126) 撞口。
- **浏览器不可用降级口径**（沿用仓库 Playwright caveat）：若本机/沙箱/离线环境无法安装或启动 Chromium，
  **如实报告环境阻塞、不伪造通过**。此时最低验证为 `playwright test --list`（解析/发现 spec，不启浏览器）
  + 既有 `typecheck`/`test`/`build` 仍绿。设 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 可复用本机已装 Chromium。

## 3. e2e 能 / 不能覆盖什么

**能**（在仿真移动视口里用真实浏览器验证）：
- 登录→首页真实跳转、DOM 交互（点击/输入/键盘事件）。
- 计算样式布局：移动视口无横向溢出、触控目标 ≥44px、安全区 **fallback 最小内边距**
  （`max(0.75rem, env(...))`/`max(0.5rem, env(...))` → 仿真设备 env=0 时取 12px/8px 最小值）。
- 暗色 token 接线（`document.documentElement.classList.contains('dark')` + body 深色背景）。

**不能**（进真机手动冒烟清单）：
- 真机真实 `env(safe-area-inset-*)`（仿真设备 inset=0，只能断言 fallback 最小值；刘海/手势条真实 inset 需真机）。
- 硬件扫码枪键盘楔入（e2e 用 `input.type()+Enter` 近似，真实扫码枪时序/焦点常驻/失焦回抢需真机）。
- Capacitor APK 内 WebView 行为、真机手势/滚动惯性/横竖屏切换。
- 像素级视觉快照（仓库无基线，PDA 设计稿未定稿——留作后续）。

## Capacitor/APK 网关基址与可复现构建

> 真机冒烟前提是先有一个**能连上网关**的 APK。详细部署口径见
> `docs/architecture/mobile-pda-deployment.md`，要点：
>
> - **网关基址**：Web/dev 留空 `VITE_NERV_IIP_API_BASE_URL`（相对 `/api/...` + vite dev proxy）；
>   **Capacitor/APK 构建必须**把它设为绝对的 BusinessGateway/PlatformGateway 基址，因为
>   APK 内 WebView **没有 dev proxy**。模板见 `frontend/apps/business-pda/.env.example`。
> - **可复现构建**：`android/` 有意 gitignore，由 `cap add android` 确定性再生；仓库基线是
>   **配置 + 脚本 + 锁定的 `@capacitor/*` 版本**。干净环境步骤（需 JDK 17 + Android SDK/`ANDROID_HOME`）：
>   `pnpm -C frontend install` → 在 `apps/business-pda`：`pnpm exec cap add android` →
>   `pnpm run cap:sync` → `cd android && ./gradlew assembleDebug`（Unix）/
>   `.\gradlew.bat assembleDebug`（Windows，平台相关手动步骤）。

## 4. 真机手动冒烟清单（每次发版前在目标 PDA 上勾验）

1. 安装 APK 启动，登录成功，首页三段（顶栏/内容/底栏）无遮挡，刘海/手势条不压内容。
2. 硬件扫码枪扫一段条码 → 扫码条捕获并显示，焦点常驻、失焦自动回抢。
3. 应用墙触控目标够大、单手拇指可达；disabled 项不可点。
4. 暗色 / 动态主色切换后整屏一致；横竖屏（若启用）无错位。
5. 弱网 / 断网下写操作有清晰失败反馈（M2 起逐页验证）。
