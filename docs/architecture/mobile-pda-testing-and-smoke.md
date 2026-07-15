# PDA（business-pda）测试层次与真机冒烟清单

> 事实依据：`frontend/apps/business-pda/`（`playwright.config.ts`、`e2e/`、`src/pages/design-system/gallery.vue`）、
> `docs/superpowers/specs/2026-06-09-mobile-pda-design.md` §13、仓库既有 Playwright caveat（同
> `docs/architecture/implementation-readiness.md` Phase 8 浏览器不可用降级口径）。

## 1. 测试层次

PDA 测试基线分两层，职责互补、不重叠（真实栈仿真走查见下文「L2 真实栈仿真走查（live）」节，
模拟器 + APK 层见「L3 Android 模拟器 + APK」节，完整分层方案见
`frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md`）：

1. **jsdom 单元/组件测试**（`vp test run src`）
   - 跑在 jsdom，无真实浏览器。覆盖：组件标记/行为/事件、store 逻辑、登录/首页页面的渲染与守卫断言。
   - 快、确定性高；但**测不到真实布局/计算样式、触控尺寸、安全区、暗色渲染、跨页导航**。

2. **Playwright e2e**（`playwright test`，真实 Chromium，移动视口 390×844 / Pixel 5）
   - 全程 `page.route` Mock BusinessGateway/console 网关（见 `e2e/fixtures.ts`），**无需后端**。
   - `seedStoredSession` 注入 `localStorage`（auth key `nerv-iip.business-pda.auth` + 可选
     `nerv-iip-color-mode`）跳过登录表单，直达受保护路由。
   - 覆盖：登录→首页真实流程、首页扫码条/应用墙/我的任务空态、`@nerv-iip/ui-mobile` 全部 5 个组件
     （AppShellMobile / ScanBar / ListRow / BottomSheet / Result，经 `/design-system/gallery` 画廊页载体）
     的真实交互、WMS/MES/设备运维三域业务链路 smoke，以及视觉/布局 smoke。

### e2e spec 清单（5 个 spec / 26 个用例）

- `e2e/app-flow.spec.ts`（5）：登录落地工作台；登录失败留在登录路由并透出错误；
  首页扫码条/空态/应用墙 + 无溢出 + 触控尺寸；应用墙入口跳转作业页；
  首页扫码 type+Enter 页内回显、不跳死路由。
- `e2e/ui-mobile.spec.ts`（8）：5 组件渲染 + 无溢出 + 触控尺寸；ScanBar 键盘楔入（type+Enter）发值；
  ScanBar blur 后回抢焦点；ScanBar 浮层打开时不抢焦、关闭后重新武装（S3）；
  ListRow 仅交互行触发 select；BottomSheet 打开 + Escape 关闭；
  AppShellMobile 安全区 fallback 最小内边距；暗色 token 接线（`.dark` + body 深色背景）。
- `e2e/wms.spec.ts`（4）：收货入库选单确认 → 成功结果；盘点录数确认 → 成功结果；
  拣货只读中文状态（无裸 code/GUID）；首页应用墙 → `/wms/inbound`。
- `e2e/mes.spec.ts`（5）：工序执行完成（二次确认）→ 成功结果；报工全链 → 成功结果；
  领料列表渲染；完工入库列表渲染；首页应用墙 → `/mes/operation`。
- `e2e/equipment.spec.ts`（4）：报修提交 → 成功结果；点检提交 → 成功结果；
  报警行详情「去报修」带参穿透报修页；首页应用墙 → `/equipment/repair`。

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
  加上既有 `typecheck`/`test`/`build` 仍绿。设 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 可复用本机已装 Chromium。

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

## L2 真实栈仿真走查（live）

> 定位：`frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md` §4 的落地
> （M1b harness 骨架）。**DOM 层键盘楔入近似**，不是硬件等价，也不改写「真机」口径
> （真机 = 目标 PDA + APK + 实体扫码枪，发版门仍是下方真机手动冒烟清单）。

- **形态**：`e2e-live/` 独立目录 + 独立 `playwright.live.config.ts`（`workers: 1`、`retries: 0`、
  `trace: 'on'`，webServer 独立端口 **5177**，`PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT` 可覆盖）。
  **无业务数据 mock**（M2 网络韧性场景仅用 `page.route`/CDP 做**传输层故障注入**，
  不伪造业务响应）：vite dev 双代理直连真实网关（`NERV_IIP_BUSINESS_GATEWAY_URL`
  默认 5119、`NERV_IIP_PLATFORM_GATEWAY_URL` 默认 5100），走真实登录页 UI 用真实 IAM 凭据登录
  （env `NERV_IIP_LIVE_USER` / `NERV_IIP_LIVE_PASSWORD`，不硬编码）。
- **扫码仿真**：`e2e-live/support/scan-gun.ts` 的 `simulateScanGun()`——不 `focus()`、不 `fill()`，
  `page.keyboard` 按 10–30ms 级字符间隔注入 + Enter 后缀，字符流必须经由 ScanBar
  「焦点常驻」契约进入输入框。
- **预检不假绿**：`e2e-live/support/preflight.ts` 先探测两个网关的匿名 `GET /health`
  （BusinessGateway/PlatformGateway `HealthEndpoint`），栈不可达时**直接 throw
  报环境阻塞**（先 `nerv.ps1 dev` 起栈），绝不 `test.skip` 静默跳过。
- **M2 网络/超时韧性**（`e2e-live/network-resilience.spec.ts`，方案 §4.2 / §8 M2）：
  3 个独立场景，全部只读（载体 = /quality/tasks 列表 + 选中任务触发的检验计划特性 GET；
  真实登录、真实数据加载完成后才注入**传输层故障**，不 mock 任何业务数据）——
  1. **离线预检**：`context.setOffline(true)`（只仿真 `navigator.onLine=false`，不代表
     Wi-Fi 抖动/DNS/TLS）→ `OfflineError` 类型化文案「当前离线，请检查网络连接后重试」
     透出错误面板（非白屏非裸堆栈，且面板保留安全重试）→ 恢复联网后可重载；
  2. **请求整体挂起 + 短超时**：`page.route` 悬挂特性 GET + `VITE_NERV_IIP_REQUEST_TIMEOUT_MS`
     短超时注入 → 「网络超时，请检查连接后重试」**数秒内**透出（不真等 30s；未注入 env
     时 spec 如实报环境阻塞而非退化长等）→ 路由释放后重试恢复；
  3. **慢网**：CDP `Network.emulateNetworkConditions`（该协议方法已标 deprecated，封装在
     `e2e-live/support/network.ts` 适配层内隔离，仅 Chromium）→ loading 态呈现、最终落定
     非错误态、同 URL 特性 GET 恰好一次（不闪断重发）。
- **短超时注入用法**：PDA `main.ts` 读 `VITE_NERV_IIP_REQUEST_TIMEOUT_MS`（毫秒）传入
  `createTimeoutFetch`。**仅 DEV 生效**（`import.meta.env.DEV === true`，即 vite dev / vitest）：
  生产 / APK 构建把 DEV 静态替换为 false，覆盖通道整体失效、无条件回落产品默认 30s；
  DEV 下钳制区间 [100, 30000]，越界回落默认（`request-timeout.ts` 生产门禁）。live webServer
  是 vite dev（DEV=true）并继承进程环境变量，在命令行注入即可（见下方命令示例）；
  `pda-live-walkthrough.ps1` 默认路径（自起 server）已默认注入 2000，注入值写入
  `run-fingerprint.txt`；`-AllowServerReuse` 复用既有 server 时该值在 server 启动时烘焙、
  无法证明已生效，脚本会 WARN「超时注入场景可能如实失败（环境阻塞）」而非静默跳过。
- **「headers 已到、body 卡死」覆盖归属**：Playwright `route.fulfill()` 是**原子**下发
  （只接受完整 body，无「先发 headers、body 流保持打开」的流式 API），live 层无法注入
  该形态、不硬造；该形态已由 L0 集成测试覆盖（真实 api-client 组合）——
  `frontend/apps/business-pda/src/api/request-timeout.integration.test.ts`（headers 200 后
  body 读取卡死 → `RequestTimeoutError` 超时文案）与
  `frontend/apps/business-pda/src/api/download-timeout.integration.test.ts`（SOP 下载 blob
  body 卡死 → 「网络超时」），live 不重复该覆盖。

```bash
# 前置：完整本地栈已运行（仓库根目录 .\nerv.ps1 dev，Docker 先开）+ live 凭据 env
# 全量含 M2 网络韧性：手跑 pnpm 入口需自带短超时注入（DEV-only，钳制 [100, 30000]），
# 否则「挂起+短超时」场景如实报环境阻塞；pwsh 写法：$env:VITE_NERV_IIP_REQUEST_TIMEOUT_MS='2000'
VITE_NERV_IIP_REQUEST_TIMEOUT_MS=2000 \
  pnpm -C frontend --filter @nerv-iip/business-pda run e2e:live

# 一键串联（worktree 归属检查 → 栈可达性 → e2e:live → 证据归集）——标准入口。
# 默认路径（自起 server）已默认注入 VITE_NERV_IIP_REQUEST_TIMEOUT_MS=2000（未显式设置时），
# 无需手动传 env；-AllowServerReuse 复用模式无法证明既有 server 带该值，脚本会 WARN。
pwsh frontend/apps/business-pda/scripts/pda-live-walkthrough.ps1

# M2 网络韧性单跑（同上需注入短超时，推荐 2000）
VITE_NERV_IIP_REQUEST_TIMEOUT_MS=2000 \
  pnpm -C frontend --filter @nerv-iip/business-pda run e2e:live -- network-resilience.spec.ts
```

- **证据包口径**（截图不得单独构成 L2 通过证据）：commit/分支指纹 + Playwright trace +
  关键请求 URL/status + 写操作幂等语义捕获 + 写操作后端状态回读 + 截图。幂等语义按链路
  代码事实如实记录：quality 提交链路**无显式幂等键**（请求体仅 inspectorUserId/resultLines/
  dispositionReason 三字段，头也无 Idempotency-Key），靠任务生命周期守门（completed 任务
  重放回读既有记录），证据记录的是「同 URL/body 重放 → 返回同一 inspectionRecordId」；
  `pda-live-walkthrough.ps1` 会把 trace/截图归集到
  `frontend/DESIGN/roadmaps/assets/<yyyy-MM-dd-HHmmss-fff>-<shortSHA>-pda-live/`（每次运行唯一目录，
  目标非空即拒绝，不混旧证据；复用既有 dev server 须显式 `-AllowServerReuse`，脚本会按
  监听进程命令行验证 worktree 归属，验不过直接拒绝，验证结论写入 metadata）。
- **写路径消耗共享 seed**：quality-execute.spec.ts 是真实业务写入——消耗一条共享 seed 的
  pending 检验任务（提交后翻 completed，不清理），重复运行需重新 seed 待检任务
  （QualitySeedService）；`runId` 数据命名空间隔离与 cleanup 归 M2，本里程碑不落地。
- **不进 CI**：依赖本地完整栈与 seed，与 `*PostgresProfileTests` 环境门控同一口径；
  显式命令运行，浏览器/栈不可用时如实报告阻塞、不伪造通过。

## L3 Android 模拟器 + APK

> 定位：`frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md` §5 / §8 M3 的落地。
> 覆盖 L2 无法触达的三类真机差异：**真实 Android WebView 内核**（非桌面 Chromium）、
> **Capacitor 原生路径**（`isNativePlatform === true`、APK 内无 dev proxy 的网关直连）、
> **系统级输入栈与 safe-area**。但仍**不是硬件等价**（方案 §2 保真等级）：`adb shell input`
> 是 Android 输入栈注入（字符流经 IME/焦点系统），仿真不了 USB HID scan code、键重复/丢键
> 与厂商扫码服务（如 Zebra DataWedge——未来接入改 `adb shell am broadcast` intent 仿真，
> 脚本留扩展位）。**不替代 L4** 实体枪与厂商 ROM，「真机」口径不变。

- **形态**：`frontend/apps/business-pda/scripts/pda-avd.ps1`（AVD 创建/启动/关停/状态）+
  `pda-adb-scan.ps1`（adb 注码 + 截图存证）。APK 构建/安装归 `pda-apk-build.ps1` 与
  `docs/architecture/mobile-pda-deployment.md`（统一网关入口、debug NSC 等 M3a 前置口径见彼处）。
- **工具链锁定**（可复现口径）：AVD `nerv-pda` = `pixel_5` 档案 +
  `system-images;android-35;google_apis;x86_64`，写死在 `pda-avd.ps1` 内，不吃隐式默认；
  脚本显式从 `ANDROID_HOME` 读 SDK（缺失报错并提示本机路径），不依赖全局安装状态。
- **加速探测不假绿**：`start` 先跑 `emulator -accel-check`——不可加速（无 WHPX/AEHD）时
  直接报错退出，确认无法启用加速时须显式 `-NoAccel` 降级为纯软件模拟（极慢但能跑），
  不静默慢跑、不伪造就绪。

```powershell
# 前置：显式设置 ANDROID_HOME（本机 Android SDK 根目录）
$env:ANDROID_HOME = 'C:\Users\hp\android-sdk'

# 1) 创建 AVD（幂等：已存在同名 AVD 直接跳过）
pwsh frontend/apps/business-pda/scripts/pda-avd.ps1 -Action create

# 2) 启动（默认带窗口便于人工走查；-Headless 无窗口 + swiftshader GPU，适合无人值守存证）：
#    轮询 sys.boot_completed=1 后输出 SERIAL=emulator-55xx 与
#    AVD/系统镜像/build/WebView 版本指纹（即证据口径的版本指纹项）
pwsh frontend/apps/business-pda/scripts/pda-avd.ps1 -Action start -Headless

# 3) 注码 + 截图存证：adb shell input text + keyevent 66（Enter 后缀，与 ScanBar 现契约一致）
pwsh frontend/apps/business-pda/scripts/pda-adb-scan.ps1 -Code 'WO-2026-0715-001' -Screencap artifacts/pda-l3/scan-echo.png

# 4) 状态 / 关停
pwsh frontend/apps/business-pda/scripts/pda-avd.ps1 -Action status
pwsh frontend/apps/business-pda/scripts/pda-avd.ps1 -Action stop
```

- **注码转义限制（如实声明）**：`input text` 没有可靠的跨版本转义通道，脚本只接受白名单
  `[A-Za-z0-9._:+,/=-]`——空格、引号、shell 元字符会被设备端解析（破坏码值甚至注入命令），
  GS1 FNC1/GS 控制字符根本无法经此通道注入；脚本对越界码值**直接拒绝**而非静默转义出一个
  「看起来像但不是」的码值（此类码值归 L4 实体枪）。且 `input text` 整段提交，无实体枪
  10–30ms 字符间隔时序（时序覆盖归 L2 `simulateScanGun` / L4）。
- **safe-area 环境前置断言（方案 §5）**：AVD 挖孔镜像**不保证** `env(safe-area-inset-*)`
  非零——Android WebView 对 CSS safe-area 的支持受 WebView 版本与 window-insets 传递方式
  影响。冒烟第一步先记录 WebView 版本 + 四个 computed inset：**inset 非零才可继续验
  safe-area**；全零则如实报「环境不具备该能力（留待 L4 真机）」，不得拿 fallback 最小
  内边距冒充真实 inset 通过验证。
- **触发时机**：发版前必跑（且周期性冷启动跑，不只发版当天首跑）；触及 Capacitor 配置、
  网关基址、扫码焦点逻辑、safe-area 的 PR 必跑。首期本地执行不进 CI（AVD 对 CI 太重）。
- **证据口径**：关键页面 screencap（`pda-adb-scan.ps1 -Screencap`，设备端 `screencap -p`
  落盘后 `adb pull`，二进制安全）+ APK SHA256（构建产物指纹，见 `pda-apk-build.ps1`）+
  AVD/系统镜像/WebView 版本指纹（`pda-avd.ps1 -Action start` 就绪时输出）+ safe-area 验证
  附 computed inset 记录（全零时记「环境不具备」）。
- **与 L4 的边界**：首期为「脚本化构建安装 + 人工按下方 L4 清单勾验 + 存证」，自动化归
  M4 spike（方案 §8，可弃）。L3 通过不改写「真机」口径（真机 = 目标 PDA + APK + 实体
  扫码枪），仿真不了实体枪 HID 电气时序与厂商 ROM WebView 差异；发版门仍是 L4 清单。

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
